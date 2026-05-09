// Healthcare Pack H-20 — Twin / IoT read-back façade.
// Provides a single Poll() entry point. Concrete BACnet / OPC-UA
// transports plug in via subclasses; this base class just shapes the
// Snapshot record. Read-only by design.

using System;
using System.Collections.Generic;

namespace StingTools.Core.Twin
{
    public class TwinSnapshot
    {
        public string DeviceId;
        public string Quantity;          // "PRESSURE_PA", "ACH", "TEMP_C", "RH_PCT", etc.
        public double Value;
        public string Unit;
        public bool InAlertBand;
        public DateTime TimestampUtc;
        public string Source;            // BACNET / OPC-UA / MODBUS / REST
    }

    public abstract class TwinReadbackBase
    {
        public abstract string ProtocolName { get; }
        public abstract IEnumerable<TwinSnapshot> Poll(IEnumerable<IoTDeviceRef> targets);
    }

    /// <summary>BACnet readback stub. Concrete BACnet/IP transport is
    /// out-of-scope for the plugin assembly (would pull in a 3rd-party
    /// stack). The hook is shipped so a project can plug a real transport
    /// behind the same interface.</summary>
    public class BacnetReadback : TwinReadbackBase
    {
        public override string ProtocolName => "BACNET";
        public override IEnumerable<TwinSnapshot> Poll(IEnumerable<IoTDeviceRef> targets)
        {
            // No-op: real BACnet transport is plug-in. Validators and the BCC
            // tab tolerate an empty result and surface "stale" state.
            return Array.Empty<TwinSnapshot>();
        }
    }

    public class OpcUaReadback : TwinReadbackBase
    {
        public override string ProtocolName => "OPC-UA";
        public override IEnumerable<TwinSnapshot> Poll(IEnumerable<IoTDeviceRef> targets) => Array.Empty<TwinSnapshot>();
    }
}
