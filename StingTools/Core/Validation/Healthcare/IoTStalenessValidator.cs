// Healthcare Pack H-20 — IoT staleness validator.
// Reports devices in IoTDeviceRegistry whose LastSeenUtc is older than
// the configured threshold. Real LastSeenUtc populates from the Twin
// transport; until that wires in, every registered device shows up so
// commissioning teams know what is *expected* to be on the network.

using Autodesk.Revit.DB;
using StingTools.Core.Twin;
using System;
using System.Collections.Generic;

namespace StingTools.Core.Validation.Healthcare
{
    public class IoTStalenessValidator : HealthcareValidatorBase
    {
        public override string Name => "IoTStalenessValidator";
        private const string Tag = "IoTStalenessValidator";
        public TimeSpan Threshold = TimeSpan.FromMinutes(30);

        public override List<ValidationResult> Validate(Document doc)
        {
            var res = new List<ValidationResult>();
            if (doc == null) return res;
            var reg = new IoTDeviceRegistry(doc);
            foreach (var d in reg.Stale(Threshold))
            {
                res.Add(new ValidationResult(d.BimElementId, ValidationSeverity.Warning,
                    "IOT.STALE",
                    $"Device {d.DeviceId} ({d.Protocol}) last seen {d.LastSeenUtc:O} — exceeds {Threshold.TotalMinutes:F0} min threshold",
                    Tag));
            }
            return res;
        }
    }
}
