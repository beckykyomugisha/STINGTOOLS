namespace StingBIM.MIM.Services;

/// <summary>
/// StingMIM Asset management service — bridges tagged BIM elements to FM asset records.
/// </summary>
public class AssetService
{
    /// <summary>
    /// Auto-generate Asset records from tagged elements.
    /// Maps TAG1 → AssetTag, TAG7A → AssetName, TAG7E → specs.
    /// </summary>
    public static Entities.Asset CreateFromTaggedElement(
        Core.Entities.TaggedElement element,
        Guid projectId)
    {
        return new Entities.Asset
        {
            ProjectId = projectId,
            TaggedElementId = element.Id,
            AssetTag = element.Tag1,
            AssetName = !string.IsNullOrEmpty(element.Tag7A)
                ? element.Tag7A
                : $"{element.FamilyName} ({element.CategoryName})",
            Building = element.Loc,
            Floor = element.Lvl,
            Room = element.RoomName,
            Zone = element.Zone,
            ConditionGrade = "A", // Default: new installation
            ConditionScore = 100,
            InstallationDate = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Calculate replacement date from installation + expected life.
    /// </summary>
    public static DateTime? CalculateReplacementDate(Entities.Asset asset)
    {
        if (!asset.InstallationDate.HasValue || !asset.ExpectedLifeYears.HasValue)
            return null;
        return asset.InstallationDate.Value.AddYears(asset.ExpectedLifeYears.Value);
    }

    /// <summary>
    /// Generate COBie-compatible asset data for FM handover.
    /// </summary>
    public static Dictionary<string, string> ToCobieComponent(Entities.Asset asset)
    {
        return new Dictionary<string, string>
        {
            ["Name"] = asset.AssetTag,
            ["CreatedBy"] = "StingMIM",
            ["CreatedOn"] = asset.CreatedAt.ToString("yyyy-MM-dd"),
            ["TypeName"] = asset.CobieType ?? "",
            ["Space"] = asset.CobieSpace ?? asset.Room ?? "",
            ["Description"] = asset.AssetName,
            ["SerialNumber"] = asset.SerialNumber ?? "",
            ["InstallationDate"] = asset.InstallationDate?.ToString("yyyy-MM-dd") ?? "",
            ["WarrantyStartDate"] = asset.WarrantyStart?.ToString("yyyy-MM-dd") ?? "",
            ["BarCode"] = asset.BarCode ?? "",
            ["AssetIdentifier"] = asset.AssetTag
        };
    }
}
