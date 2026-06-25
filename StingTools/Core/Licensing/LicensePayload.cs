using System.Text.Json;
using System.Text.Json.Serialization;

namespace StingTools.Core.Licensing
{
    public sealed class LicensePayload
    {
        [JsonPropertyName("licenseId")]   public string LicenseId { get; set; }
        [JsonPropertyName("machineCode")] public string MachineCode { get; set; }
        [JsonPropertyName("licensee")]    public string Licensee { get; set; }
        [JsonPropertyName("issuedUnix")]  public long IssuedUnix { get; set; }
        [JsonPropertyName("expiryUnix")]  public long ExpiryUnix { get; set; }
        [JsonPropertyName("schema")]      public int Schema { get; set; } = 1;

        private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions { WriteIndented = false };
        public string ToJson() => JsonSerializer.Serialize(this, Opts);
        public static LicensePayload FromJson(string json) => JsonSerializer.Deserialize<LicensePayload>(json);
    }
}
