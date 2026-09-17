namespace StingTools.Core
{
    /// <summary>
    /// How tagging treats an element that already carries a complete tag.
    ///
    /// Relocated out of the oversized TagConfig.cs, which is Revit-bound and so drags
    /// the whole Revit API into anything that wants this enum — the same move already
    /// made for SeqScheme (now in Core/SeqAssigner.cs). Same namespace, so no call site
    /// changes. Keeping it here lets TaggingModels.cs be compiled by the test project.
    /// </summary>
    public enum TagCollisionMode
    {
        /// <summary>Auto-increment SEQ until a unique tag is found (default).</summary>
        AutoIncrement,
        /// <summary>Skip elements that already have a complete tag — do not modify.</summary>
        Skip,
        /// <summary>Overwrite existing tags with newly generated values.</summary>
        Overwrite,
    }
}
