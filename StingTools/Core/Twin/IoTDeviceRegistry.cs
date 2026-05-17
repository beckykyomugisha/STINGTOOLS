// Healthcare Pack H-20 — IoT device registry.
// Maps BIM element IDs to IoT device endpoints by walking elements
// that carry an ICT_HEALTHIOT_DEVICE_ID_TXT (or equivalent) and a
// protocol token (BACNET / OPC-UA / MODBUS / REST / PROPRIETARY).

using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core;

namespace StingTools.Core.Twin
{
    public class IoTDeviceRef
    {
        public ElementId BimElementId;
        public string DeviceId;
        public string Protocol;        // BACNET / OPC-UA / MODBUS / REST / PROPRIETARY
        public string EndpointAddress; // e.g. "bacnet://10.10.5.7/12345/AI/3"
        public string AlertBand;       // free text: "5–25 Pa", "20–60 °C"
        public DateTime LastSeenUtc;
    }

    public class IoTDeviceRegistry
    {
        private readonly Document _doc;
        private List<IoTDeviceRef> _devices;

        public IoTDeviceRegistry(Document doc) { _doc = doc; }

        public IEnumerable<IoTDeviceRef> All() { EnsureLoaded(); return _devices; }

        public IEnumerable<IoTDeviceRef> ForRoom(ElementId roomId)
        {
            EnsureLoaded();
            return _devices.Where(d =>
            {
                try
                {
                    var el = _doc.GetElement(d.BimElementId);
                    if (el is FamilyInstance fi)
                    {
                        if (fi.Room?.Id?.Value == roomId.Value) return true;
                        if (fi.Location is LocationPoint lp)
                        {
                            var room = _doc.GetRoomAtPoint(lp.Point);
                            return room?.Id?.Value == roomId.Value;
                        }
                    }
                } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                return false;
            });
        }

        public IEnumerable<IoTDeviceRef> Stale(TimeSpan threshold)
        {
            EnsureLoaded();
            var now = DateTime.UtcNow;
            return _devices.Where(d => d.LastSeenUtc != default && now - d.LastSeenUtc > threshold);
        }

        private void EnsureLoaded()
        {
            if (_devices != null) return;
            _devices = new List<IoTDeviceRef>();
            if (_doc == null) return;

            var cats = new[] {
                BuiltInCategory.OST_DataDevices, BuiltInCategory.OST_CommunicationDevices,
                BuiltInCategory.OST_SecurityDevices, BuiltInCategory.OST_FireAlarmDevices,
                BuiltInCategory.OST_SpecialityEquipment, BuiltInCategory.OST_MedicalEquipment,
                BuiltInCategory.OST_NurseCallDevices, BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_PlumbingFixtures,
                BuiltInCategory.OST_GenericModel
            };
            var f = new ElementMulticategoryFilter(cats);
            foreach (var el in new FilteredElementCollector(_doc).WherePasses(f).WhereElementIsNotElementType())
            {
                var deviceId = Get(el, "ICT_HEALTHIOT_DEVICE_ID_TXT")
                            ?? Get(el, "MGS_AAP_REF_TXT")
                            ?? Get(el, "MGS_ZVB_REF_TXT");
                if (string.IsNullOrEmpty(deviceId)) continue;
                _devices.Add(new IoTDeviceRef
                {
                    BimElementId = el.Id,
                    DeviceId = deviceId,
                    Protocol = Get(el, "ICT_HEALTHIOT_PROTOCOL_TXT") ?? "PROPRIETARY",
                    EndpointAddress = Get(el, "ICT_HEALTHIOT_ENDPOINT_TXT") ?? "",
                    AlertBand = Get(el, "ICT_HEALTHIOT_ALERT_BAND_TXT") ?? "",
                    LastSeenUtc = default
                });
            }
        }

        private static string Get(Element el, string n)
        {
            try
            {
                var p = el.LookupParameter(n);
                if (p == null || !p.HasValue) return null;
                if (p.StorageType == StorageType.String) return p.AsString();
                return p.AsValueString();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }
    }
}
